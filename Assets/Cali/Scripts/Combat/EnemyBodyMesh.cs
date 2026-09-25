using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Swap the enemy look by assigning a mesh (and optional materials) on the prefab.
    /// Behavior stays on the root; this only drives the Visual child.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class EnemyBodyMesh : MonoBehaviour
    {
        [Tooltip("Drop any Mesh here to change how this enemy looks.")]
        public Mesh bodyMesh;

        [Tooltip("Optional. Leave empty to keep the Visual renderer's current material.")]
        public Material[] bodyMaterials;

        [Tooltip("Child MeshFilter. Auto-finds a child named Visual if empty.")]
        public MeshFilter bodyFilter;

        [Tooltip("Local offset of the visual. Use this if the mesh pivot is not at the feet.")]
        public Vector3 visualOffset = new(0f, 1f, 0f);

        public Vector3 visualScale = Vector3.one;

        [Tooltip("Resize the root capsule collider to match the mesh.")]
        public bool fitCollider = true;

        public float colliderPadding = 0.05f;

        void Reset()
        {
            Bind();
            Apply();
        }

        void OnValidate()
        {
            Bind();
            Apply();
        }

        void Awake()
        {
            Bind();
            Apply();
        }

        void Bind()
        {
            if (bodyFilter == null)
            {
                var visual = transform.Find("Visual");
                if (visual != null)
                    bodyFilter = visual.GetComponent<MeshFilter>();
                if (bodyFilter == null)
                    bodyFilter = GetComponentInChildren<MeshFilter>(true);
            }

            if (bodyMesh == null && bodyFilter != null)
                bodyMesh = bodyFilter.sharedMesh;
        }

        public void Apply()
        {
            if (bodyFilter == null)
                return;

            if (bodyMesh != null)
                bodyFilter.sharedMesh = bodyMesh;

            var rend = bodyFilter.GetComponent<MeshRenderer>();
            if (rend != null && bodyMaterials != null && bodyMaterials.Length > 0 && bodyMaterials[0] != null)
                rend.sharedMaterials = bodyMaterials;

            var visual = bodyFilter.transform;
            visual.localPosition = visualOffset;
            visual.localRotation = Quaternion.identity;
            visual.localScale = visualScale;

            if (fitCollider)
                FitCapsuleToMesh();
        }

        void FitCapsuleToMesh()
        {
            if (bodyMesh == null)
                return;

            var col = GetComponent<CapsuleCollider>();
            if (col == null)
                return;

            Bounds b = bodyMesh.bounds;
            Vector3 size = Vector3.Scale(b.size, visualScale);
            Vector3 center = visualOffset + Vector3.Scale(b.center, visualScale);

            int axis = 1;
            float height = size.y;
            float radius = Mathf.Max(size.x, size.z) * 0.5f;
            if (size.z >= size.x && size.z >= size.y)
            {
                axis = 2;
                height = size.z;
                radius = Mathf.Max(size.x, size.y) * 0.5f;
            }
            else if (size.x >= size.y && size.x >= size.z)
            {
                axis = 0;
                height = size.x;
                radius = Mathf.Max(size.y, size.z) * 0.5f;
            }

            col.direction = axis;
            col.radius = Mathf.Max(0.05f, radius + colliderPadding);
            col.height = Mathf.Max(col.radius * 2f, height + colliderPadding * 2f);
            col.center = center;
        }
    }
}
