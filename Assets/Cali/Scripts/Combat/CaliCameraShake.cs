using Unity.Cinemachine;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Small Cinemachine impulse for chain kills. Collision smoothing lives on the CM camera prefab.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class CaliCameraShake : MonoBehaviour
    {
        public static CaliCameraShake Instance { get; private set; }

        [Tooltip("Impulse duration in seconds.")]
        public float killDuration = 0.12f;

        [Tooltip("Impulse strength. 1 is a full bump.")]
        public float killForce = 0.32f;

        CinemachineImpulseSource _source;

        public static CaliCameraShake Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<CaliCameraShake>();
            if (existing != null)
            {
                Instance = existing;
                existing.EnsureListener();
                return existing;
            }

            var go = new GameObject("CaliCameraShake");
            return go.AddComponent<CaliCameraShake>();
        }

        public static void PlayKill()
        {
            Ensure();
            if (Instance != null)
                Instance.FireKill();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SetupSource();
            EnsureListener();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void SetupSource()
        {
            _source = GetComponent<CinemachineImpulseSource>();
            if (_source == null)
                _source = gameObject.AddComponent<CinemachineImpulseSource>();

            _source.ImpulseDefinition = new CinemachineImpulseDefinition
            {
                ImpulseChannel = 1,
                ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
                ImpulseDuration = Mathf.Max(0.04f, killDuration),
                ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
                DissipationDistance = 100f,
                DissipationRate = 0.25f,
                PropagationSpeed = 343f
            };
            _source.DefaultVelocity = new Vector3(0.08f, 0.28f, 0f);
        }

        void EnsureListener()
        {
            var cams = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null)
                    continue;
                if (cam.GetComponent<CinemachineImpulseListener>() != null)
                    continue;

                var listener = cam.gameObject.AddComponent<CinemachineImpulseListener>();
                listener.ApplyAfter = CinemachineCore.Stage.Noise;
                listener.ChannelMask = 1;
                listener.Gain = 1f;
                listener.UseCameraSpace = true;
            }
        }

        void FireKill()
        {
            if (_source == null)
                SetupSource();

            _source.ImpulseDefinition.ImpulseDuration = Mathf.Max(0.04f, killDuration);
            _source.GenerateImpulseWithForce(Mathf.Max(0.05f, killForce));
        }
    }
}
