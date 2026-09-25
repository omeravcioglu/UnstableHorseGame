using UnityEngine;
using UnityEngine.Rendering;

namespace Cali.Gameplay
{
    /// <summary>
    /// Renders one end-to-end chain mesh along a hanging polyline. Gameplay simulation
    /// is untouched; this only deforms a readable mesh copy. The mesh is never tiled
    /// or sliced, so length changes cannot pop extra copies into view.
    /// </summary>
    public sealed class IronChainMesh
    {
        const float NormalMoveSq = 0.0004f;

        Mesh _runtime;
        Vector3[] _bind;
        Vector3[] _work;
        Vector3[] _off;
        Vector3[] _offNative;
        float[] _u;
        int[] _tris;
        Vector2[] _uv;
        float _bindLen;
        float _bindLenNative;
        float _fittedRest = -1f;
        float _radiusScale = 1f;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Transform _root;
        bool _ready;
        Vector3[] _lastPath;
        float[] _cum;

        public bool Ready => _ready;

        public void SetThickness(float thickness)
        {
            _radiusScale = Mathf.Max(0.25f, thickness);
        }

        public bool IsVisible =>
            _ready && _renderer != null && _renderer.enabled && _runtime != null
            && _runtime.vertexCount > 0 && _runtime.bounds.size.sqrMagnitude > 0.0001f;

        public bool Setup(Transform parent, GameObject prefab, Material material)
        {
            Release();
            if (parent == null || prefab == null)
                return false;

            if (!ExtractBind(prefab, material, out Material[] mats))
                return false;

            return FinishSetup(parent, mats);
        }

        public bool Setup(Transform parent, Mesh mesh, Material material)
        {
            Release();
            if (parent == null || mesh == null)
                return false;

            if (!BindFromMesh(mesh, Matrix4x4.identity, material, out Material[] mats))
                return false;

            return FinishSetup(parent, mats);
        }

        /// <summary>
        /// Scale the bind once so the modeled chain length matches the tether rest length.
        /// Thickness scales with it so a huge/tiny FBX still looks like one chain.
        /// </summary>
        public void FitRestLength(float rest)
        {
            if (!_ready || _offNative == null || _bindLenNative < 0.001f || rest < 0.05f)
                return;
            if (Mathf.Abs(_fittedRest - rest) < 0.01f)
                return;

            float s = rest / _bindLenNative;
            _bindLen = rest;
            _fittedRest = rest;
            for (int i = 0; i < _offNative.Length; i++)
                _off[i] = _offNative[i] * s;
        }

        bool FinishSetup(Transform parent, Material[] mats)
        {
            var go = new GameObject("IronChainMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _root = go.transform;
            _filter = go.AddComponent<MeshFilter>();
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterials = mats;
            _renderer.shadowCastingMode = ShadowCastingMode.On;
            _renderer.receiveShadows = true;
            go.AddComponent<ChainIgnore>();

            _runtime = new Mesh { name = "IronChainRuntime" };
            _runtime.MarkDynamic();
            if (_bind.Length > 65535)
                _runtime.indexFormat = IndexFormat.UInt32;
            _work = new Vector3[_bind.Length];
            _runtime.SetVertices(_work);
            if (_uv != null)
                _runtime.SetUVs(0, _uv);
            _runtime.SetTriangles(_tris, 0);
            _filter.sharedMesh = _runtime;
            _ready = true;
            return true;
        }

        public void Hide()
        {
            if (_renderer != null)
                _renderer.enabled = false;
        }

        public void Deform(Vector3[] path)
        {
            if (!_ready || path == null || path.Length < 2)
                return;

            _renderer.enabled = true;
            BuildCumulative(path, out float pathLen);
            if (pathLen < 0.05f)
            {
                _renderer.enabled = false;
                return;
            }

            // Never stretch the modeled chain past rest length. Extra path is ignored.
            float usedLen = _bindLen > 0.05f ? Mathf.Min(pathLen, _bindLen) : pathLen;

            Vector3 transport = Vector3.up;
            Vector3 prevTangent = Vector3.zero;
            for (int i = 0; i < _bind.Length; i++)
            {
                float s = _u[i] * usedLen;
                Sample(path, _cum, usedLen, s, transport, out Vector3 p, out Vector3 tangent, out Vector3 n, out Vector3 b);
                if (prevTangent.sqrMagnitude > 1e-6f && Vector3.Dot(tangent, prevTangent) < 0.12f)
                    tangent = prevTangent;
                prevTangent = tangent;
                if (tangent.sqrMagnitude > 1e-8f)
                {
                    Vector3 nextUp = Vector3.Cross(b, tangent);
                    if (nextUp.sqrMagnitude > 1e-8f)
                        transport = Vector3.Slerp(transport, nextUp.normalized, 0.7f).normalized;
                }

                _work[i] = _root.InverseTransformPoint(
                    p + (n * _off[i].x + b * _off[i].y) * _radiusScale);
            }

            _runtime.SetVertices(_work);
            _runtime.RecalculateBounds();

            if (PathMovedEnough(path))
            {
                _runtime.RecalculateNormals();
                _runtime.RecalculateTangents();
            }
        }

        bool PathMovedEnough(Vector3[] path)
        {
            bool moved = _lastPath == null || _lastPath.Length != path.Length;
            if (!moved)
            {
                for (int i = 0; i < path.Length; i++)
                {
                    if ((path[i] - _lastPath[i]).sqrMagnitude > NormalMoveSq)
                    {
                        moved = true;
                        break;
                    }
                }
            }

            if (_lastPath == null || _lastPath.Length != path.Length)
                _lastPath = new Vector3[path.Length];
            System.Array.Copy(path, _lastPath, path.Length);
            return moved;
        }

        public void Release()
        {
            _ready = false;
            _fittedRest = -1f;
            if (_runtime != null)
                Object.Destroy(_runtime);
            _runtime = null;
            if (_root != null)
                Object.Destroy(_root.gameObject);
            _root = null;
            _filter = null;
            _renderer = null;
            _bind = null;
            _work = null;
            _off = null;
            _offNative = null;
            _lastPath = null;
            _cum = null;
        }

        bool ExtractBind(GameObject prefab, Material material, out Material[] mats)
        {
            mats = null;
            var inst = Object.Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            inst.SetActive(true);

            var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0 && renderers[0].sharedMaterials != null && renderers[0].sharedMaterials.Length > 0)
                mats = renderers[0].sharedMaterials;
            if (material != null)
                mats = new[] { material };

            var filters = inst.GetComponentsInChildren<MeshFilter>(true);
            Mesh source = null;
            Matrix4x4 xform = Matrix4x4.identity;
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null || filters[i].sharedMesh == null)
                    continue;
                source = filters[i].sharedMesh;
                xform = filters[i].transform.localToWorldMatrix;
                break;
            }

            if (source == null)
            {
                Object.Destroy(inst);
                return false;
            }

            bool ok = BindFromMesh(source, xform, material, out Material[] bindMats);
            if (bindMats != null)
                mats = bindMats;
            Object.Destroy(inst);
            return ok;
        }

        bool BindFromMesh(Mesh source, Matrix4x4 xform, Material material, out Material[] mats)
        {
            mats = material != null ? new[] { material } : null;

            Vector3[] verts;
            try
            {
                verts = source.vertices;
            }
            catch
            {
                return false;
            }

            if (verts == null || verts.Length < 8)
                return false;

            for (int i = 0; i < verts.Length; i++)
                verts[i] = xform.MultiplyPoint3x4(verts[i]);

            Bounds b = new Bounds(verts[0], Vector3.zero);
            for (int i = 1; i < verts.Length; i++)
                b.Encapsulate(verts[i]);

            int axis = 0;
            if (b.size.y >= b.size.x && b.size.y >= b.size.z)
                axis = 1;
            else if (b.size.z >= b.size.x && b.size.z >= b.size.y)
                axis = 2;

            float min = b.min[axis];
            float span = Mathf.Max(0.001f, b.size[axis]);
            _bindLenNative = span;
            _bindLen = span;
            _bind = verts;
            _u = new float[verts.Length];
            _off = new Vector3[verts.Length];
            _offNative = new Vector3[verts.Length];
            Vector3 center = b.center;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                _u[i] = Mathf.Clamp01((v[axis] - min) / span);
                Vector3 radial;
                if (axis == 1)
                    radial = new Vector3(v.x - center.x, v.z - center.z);
                else if (axis == 2)
                    radial = new Vector3(v.x - center.x, v.y - center.y);
                else
                    radial = new Vector3(v.y - center.y, v.z - center.z);
                _offNative[i] = radial;
                _off[i] = radial;
            }

            _tris = source.triangles;
            var uv = source.uv;
            _uv = uv != null && uv.Length == verts.Length ? uv : null;

            if (mats == null || mats.Length == 0 || mats[0] == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mats = new[] { new Material(shader) { color = new Color(0.28f, 0.27f, 0.25f) } };
            }

            return _tris != null && _tris.Length >= 3;
        }

        void BuildCumulative(Vector3[] path, out float pathLen)
        {
            if (_cum == null || _cum.Length != path.Length)
                _cum = new float[path.Length];
            pathLen = 0f;
            _cum[0] = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                pathLen += Vector3.Distance(path[i - 1], path[i]);
                _cum[i] = pathLen;
            }
        }

        static void Sample(
            Vector3[] path,
            float[] cum,
            float pathLen,
            float s,
            Vector3 transport,
            out Vector3 p,
            out Vector3 tangent,
            out Vector3 n,
            out Vector3 b)
        {
            s = Mathf.Clamp(s, 0f, pathLen);
            int i = 1;
            while (i < cum.Length - 1 && cum[i] < s)
                i++;

            float prev = cum[i - 1];
            float seg = Mathf.Max(0.0001f, cum[i] - prev);
            float t = (s - prev) / seg;
            p = Vector3.Lerp(path[i - 1], path[i], t);
            tangent = path[i] - path[i - 1];
            if (tangent.sqrMagnitude < 1e-8f)
                tangent = i + 1 < path.Length ? path[i + 1] - path[i] : Vector3.forward;
            tangent.Normalize();

            b = Vector3.Cross(tangent, transport);
            if (b.sqrMagnitude < 1e-6f)
                b = Vector3.Cross(tangent, Vector3.right);
            b.Normalize();
            n = Vector3.Cross(b, tangent).normalized;
        }
    }
}
