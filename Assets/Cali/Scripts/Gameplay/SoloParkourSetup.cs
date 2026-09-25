using System.Collections;
using MalbersAnimations;
using MalbersAnimations.Controller;
using MalbersAnimations.InputSystem;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cali.Gameplay
{
    /// <summary>
    /// Self-contained one-horse parkour: WASD, mouse look, Space jump.
    /// Does not depend on Malbers Player Input or Cinemachine staying live.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SoloParkourSetup : MonoBehaviour
    {
        public MAnimal horse;
        public float cameraDistance = 8.5f;
        public float cameraHeight = 1.7f;
        public float lookSensitivity = 0.12f;

        Transform _focus;
        CinemachineBrain _brain;
        float _yaw;
        float _pitch = 12f;
        bool _jumpWas;
        bool _groundedOnce;

        void Awake()
        {
            if (horse == null)
                horse = FindFirstObjectByType<MAnimal>();

            if (horse != null)
            {
                var link = horse.GetComponent<MInputLink>();
                if (link == null)
                    link = horse.GetComponentInChildren<MInputLink>();
                if (link != null)
                    link.enabled = false;
            }

            _brain = FindFirstObjectByType<CinemachineBrain>();
            if (_brain != null)
                _brain.enabled = false;

            DisableCinemachineCameras();
            CloseAllLods();
        }

        IEnumerator Start()
        {
            if (horse == null)
                horse = FindFirstObjectByType<MAnimal>();
            if (horse == null)
            {
                Debug.LogError("[SoloParkour] No horse (MAnimal) in the scene.", this);
                yield break;
            }

            EnableCameraTarget();
            _focus = FindFocus();
            _yaw = horse.transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            yield return new WaitForFixedUpdate();
            HorsePhysics.Apply(horse);
            HorseJump.Prepare(horse);
            PlaceOnGround();
            yield return null;
            PlaceOnGround();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }

            if (horse == null)
                return;

            if (!_groundedOnce)
                PlaceOnGround();

            Vector3 stick3 = ReadWasd();
            Vector2 stick = new Vector2(stick3.x, stick3.z);
            bool air = HorseJump.UseAirSteer(horse);
            if (air)
            {
                horse.Move(Vector3.zero);
                horse.SetSprint(false);
            }
            else
            {
                Vector3 move = Cali.Network.CoopInput.CameraRelativeMove(stick);
                horse.Move(move);
                horse.SetSprint(Cali.Network.CoopInput.ReadSprintHeld() && move.sqrMagnitude > 0.04f);
            }
            HorseJump.UpdateHeld(horse, Cali.Network.CoopInput.ReadJumpHeld(), stick, ref _jumpWas);
        }

        void LateUpdate()
        {
            if (horse == null)
                return;

            if (_brain != null)
                _brain.enabled = false;

            var cam = Camera.main;
            if (cam == null || cam.pixelWidth < 2 || cam.pixelHeight < 2)
                return;

            if (_focus == null)
                _focus = FindFocus();

            Vector2 look = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                look = mouse.delta.ReadValue();

            _yaw += look.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * lookSensitivity, -25f, 55f);

            Vector3 pivot = horse.transform.position + Vector3.up * cameraHeight;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 camPos = pivot + rot * (Vector3.back * cameraDistance);

            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(pivot - camPos, Vector3.up));
        }

        static void CloseAllLods()
        {
            var groups = FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null)
                    groups[i].ForceLOD(0);
            }
        }

        static void DisableCinemachineCameras()
        {
            var vcams = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < vcams.Length; i++)
            {
                if (vcams[i] != null)
                    vcams[i].gameObject.SetActive(false);
            }
        }

        void EnableCameraTarget()
        {
            var selector = horse.GetComponent<ComponentSelector>();
            if (selector != null && selector.internalComponents != null)
            {
                for (int i = 0; i < selector.internalComponents.Count; i++)
                {
                    var set = selector.internalComponents[i];
                    if (set == null || set.name != "Camera Target")
                        continue;
                    set.active = true;
                    if (set.gameObjects == null)
                        continue;
                    for (int g = 0; g < set.gameObjects.Length; g++)
                    {
                        if (set.gameObjects[g] != null)
                            set.gameObjects[g].SetActive(true);
                    }
                }
            }

            var all = horse.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "CameraTarget Horse")
                    all[i].gameObject.SetActive(true);
            }
        }

        Transform FindFocus()
        {
            var all = horse.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "CameraTarget Horse")
                    return all[i];
            }
            return horse.transform;
        }

        void PlaceOnGround()
        {
            Vector3 origin = horse.transform.position + Vector3.up * 20f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.NegativeInfinity;
            Vector3 best = Vector3.zero;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform == horse.transform || hit.collider.transform.IsChildOf(horse.transform))
                    continue;
                if (hit.normal.y < 0.35f)
                    continue;
                if (hit.point.y > bestY)
                {
                    bestY = hit.point.y;
                    best = hit.point;
                }
            }

            if (bestY > float.NegativeInfinity)
            {
                horse.Teleport(best);
                _groundedOnce = true;
            }
        }

        static Vector3 ReadWasd()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return Vector3.zero;

            float x = 0f;
            float z = 0f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.sKey.isPressed) z -= 1f;
            if (kb.wKey.isPressed) z += 1f;
            return new Vector3(x, 0f, z);
        }
    }
}
