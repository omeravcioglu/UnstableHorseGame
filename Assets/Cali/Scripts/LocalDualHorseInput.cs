using MalbersAnimations.Controller;
using MalbersAnimations.InputSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cali.Gameplay
{
    /// <summary>
    /// Offline dual control: Horse A = WASD, Horse B = Arrow Keys (Enter to jump).
    /// Disables the shared Malbers PlayerInput so both horses are not driven by the same device.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class LocalDualHorseInput : MonoBehaviour
    {
        [Header("Horses")]
        [Tooltip("Player 1 — WASD. Leave empty to auto-find 'Horse Realistic'.")]
        public MAnimal horseA;

        [Tooltip("Player 2 — Arrow Keys. Leave empty / off for single-horse scenes.")]
        public MAnimal horseB;

        [Header("Setup")]
        [Tooltip("When off, Horse B is left inactive and the chain is not used.")]
        public bool allowSecondHorse = true;
        [Tooltip("Disable shared PlayerInput / MInputLink so only this script drives movement.")]
        public bool takeOverInput = true;

        [Tooltip("Deactivate other horses in the demo scene (Pegasus, Poly Art, etc.).")]
        public bool deactivateExtraHorses = true;

        [Header("Debug")]
        public bool logSetup = true;

        void Awake()
        {
            if (CaliSinglePlayer.IsActiveScene)
                allowSecondHorse = false;

            ResolveHorses();
            if (takeOverInput)
                TakeOverInput();
            if (deactivateExtraHorses)
                DeactivateExtras();

            // Fusion NetHorse owns both seats while a session is running (solo host
            // still maps Unicorn to arrows). Stay off so the two drivers cannot stack.
            if (Cali.Network.CoopSessionStarter.IsOnline)
                enabled = false;

            if (GetComponent<HorsePegasusForm>() == null && FindFirstObjectByType<HorsePegasusForm>() == null)
                gameObject.AddComponent<HorsePegasusForm>();

            HorsePhysics.ApplyAll();
        }

        void Start()
        {
            if (Cali.Network.CoopSessionStarter.IsOnline)
                return;

            HorseJump.Prepare(horseA, JumpStateId);
            HorseJump.Prepare(horseB, JumpStateId);

            if (horseA != null)
                Cali.Network.CoopCameraFocus.FocusOnHorse(horseA);
        }

        /// <summary>
        /// Online the horses are driven by NetHorse, which owns its own tether. Leaving these
        /// subscribed would apply the rope twice on the same step.
        /// </summary>
        void OnDisable()
        {
            _tetherA.Release();
            _tetherB.Release();
        }

        void Update()
        {
            if (Cali.Network.CoopSessionStarter.IsOnline)
            {
                _tetherA.Release();
                _tetherB.Release();
                return;
            }

            if (horseA == null || (allowSecondHorse && horseB == null))
                ResolveHorses();

            bool idle = CaliAchievementCamera.BlocksHorseInput;

            Vector3 moveA = Vector3.zero;
            Vector3 moveB = Vector3.zero;

            if (horseA != null)
            {
                Vector2 stick = idle ? Vector2.zero : Cali.Network.CoopInput.ReadWasd();
                bool air = HorseJump.UseAirSteer(horseA);
                if (air)
                {
                    horseA.Move(Vector3.zero);
                    horseA.SetSprint(false);
                    moveA = Vector3.ProjectOnPlane(horseA.Forward, Vector3.up);
                }
                else
                {
                    moveA = Cali.Network.CoopInput.CameraRelativeMove(stick);
                    horseA.Move(moveA);
                    horseA.SetSprint(!idle && Cali.Network.CoopInput.ReadSprintHeld() && moveA.sqrMagnitude > 0.04f);
                }
                HorseJump.UpdateHeld(horseA, !idle && Cali.Network.CoopInput.ReadJumpHeld(), stick, ref _jumpAWas, JumpStateId);
            }

            if (horseB != null)
            {
                Vector2 stick = idle ? Vector2.zero : Cali.Network.CoopInput.ReadArrows();
                bool air = HorseJump.UseAirSteer(horseB);
                if (air)
                {
                    horseB.Move(Vector3.zero);
                    horseB.SetSprint(false);
                    moveB = Vector3.ProjectOnPlane(horseB.Forward, Vector3.up);
                }
                else
                {
                    moveB = Cali.Network.CoopInput.CameraRelativeMove(stick);
                    horseB.Move(moveB);
                    horseB.SetSprint(!idle && Cali.Network.CoopInput.ReadSprintHeldB() && moveB.sqrMagnitude > 0.04f);
                }
                HorseJump.UpdateHeld(horseB, !idle && Cali.Network.CoopInput.ReadJumpHeldB(), stick, ref _jumpBWas, JumpStateId);
            }

            if (allowSecondHorse)
            {
                TrackChain(_tetherA, horseA, horseB, moveA, moveB);
                TrackChain(_tetherB, horseB, horseA, moveB, moveA);
            }
            else
            {
                _tetherA.Release();
                _tetherB.Release();
            }
        }

        /// <summary>
        /// Offline runs the same rope as online: one tether per horse, each shortening only its
        /// own horse's step, so the two paths cannot drift apart in feel.
        /// </summary>
        static void TrackChain(ChainTether tether, MAnimal self, MAnimal partner, Vector3 selfWish, Vector3 partnerWish)
        {
            if (self == null || partner == null)
            {
                tether.Release();
                return;
            }

            tether.Bind(self);
            HorseBody.SetSimulated(self, true);

            var chain = SoftHorseChain.Instance;
            if (chain != null)
                tether.Length = chain.maxLength;

            tether.Track(selfWish, partner.transform.position, partnerWish);
        }

        const int JumpStateId = 2; // Malbers StatesID/Jump.asset

        readonly ChainTether _tetherA = new ChainTether();
        readonly ChainTether _tetherB = new ChainTether();

        static bool _jumpAWas;
        static bool _jumpBWas;

        void ResolveHorses()
        {
            if (horseA == null)
                horseA = FindAnimalByName("Horse Realistic");
            if (allowSecondHorse && horseB == null)
                horseB = FindAnimalByName("Horse Unicorn");
            if (!allowSecondHorse)
                horseB = null;

            if (horseA != null && !horseA.gameObject.activeInHierarchy)
                horseA.gameObject.SetActive(true);
            if (allowSecondHorse && horseB != null && !horseB.gameObject.activeInHierarchy)
                horseB.gameObject.SetActive(true);

            if (logSetup)
            {
                Debug.Log($"[LocalDualHorseInput] Horse A (WASD): {(horseA ? horseA.name : "MISSING")}", this);
                Debug.Log($"[LocalDualHorseInput] Horse B (Arrows): {(horseB ? horseB.name : "MISSING")}", this);
            }
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

        public static void ClaimExclusiveControl()
        {
            var duals = FindObjectsByType<LocalDualHorseInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < duals.Length; i++)
            {
                if (duals[i] == null)
                    continue;
                duals[i].TakeOverInput();
                if (Cali.Network.CoopSessionStarter.IsOnline)
                    duals[i].enabled = false;
            }

            if (duals.Length == 0)
                DisableMalbersSources();
        }

        public static void DisableMalbersSources()
        {
            var playerInputs = FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var pi in playerInputs)
            {
                if (pi != null)
                    pi.enabled = false;
            }

            var links = FindObjectsByType<MInputLink>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var link in links)
            {
                if (link != null)
                    link.enabled = false;
            }
        }

        void TakeOverInput() => DisableMalbersSources();

        void DeactivateExtras()
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var animal in animals)
            {
                if (animal == horseA || animal == horseB)
                    continue;
                if (animal.GetComponentInParent<Cali.Combat.ChainKillableEnemy>() != null)
                    continue;
                if (animal.GetComponentInParent<CaliNpcHorse>() != null)
                    continue;

                animal.gameObject.SetActive(false);
                if (logSetup)
                    Debug.Log($"[LocalDualHorseInput] Deactivated extra horse '{animal.name}'", animal);
            }
        }
    }
}
