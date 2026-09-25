using UnityEngine;

namespace Cali.Rendering
{
    /// <summary>
    /// Gives individual layers a shorter draw distance than the camera's far clip plane, so small
    /// decorative geometry stops being submitted long before the terrain and buildings do. This is
    /// the one part of frustum culling that is actually tunable — the culling itself is automatic.
    ///
    /// It starts out empty and does nothing until layers are listed here, because this project
    /// keeps all its scenery on the Default layer. Culling Default would take the whole world with
    /// it, so making this pay off means first moving small props onto a layer of their own.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraLayerCullDistances : MonoBehaviour
    {
        [System.Serializable]
        public struct LayerDistance
        {
            [Tooltip("Layer name exactly as it appears in Tags & Layers.")]
            public string layer;

            [Tooltip("Metres. 0 falls back to the camera's far clip plane.")]
            public float distance;
        }

        [Tooltip("Per-layer draw distances. Layers not listed keep the camera's far clip plane.")]
        public LayerDistance[] layers = new LayerDistance[0];

        [Tooltip("Cull on distance from the camera rather than the frustum's far plane. " +
                 "Stops props popping as the camera turns.")]
        public bool spherical = true;

        void OnEnable() => Apply();

        void OnValidate()
        {
            if (isActiveAndEnabled)
                Apply();
        }

        [ContextMenu("Apply Now")]
        public void Apply()
        {
            var cam = GetComponent<Camera>();
            if (cam == null || layers == null)
                return;

            float[] distances = cam.layerCullDistances;
            for (int i = 0; i < layers.Length; i++)
            {
                int index = LayerMask.NameToLayer(layers[i].layer);
                if (index < 0 || index >= distances.Length)
                {
                    Debug.LogWarning($"[CameraLayerCullDistances] No layer named '{layers[i].layer}'", this);
                    continue;
                }

                distances[index] = Mathf.Max(0f, layers[i].distance);
            }

            cam.layerCullSpherical = spherical;
            cam.layerCullDistances = distances;
        }
    }
}
